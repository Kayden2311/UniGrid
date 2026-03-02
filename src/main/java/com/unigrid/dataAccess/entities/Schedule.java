package com.unigrid.dataAccess.entities;

import jakarta.persistence.*;
import lombok.Getter;
import lombok.Setter;
import org.hibernate.annotations.ColumnDefault;
import org.hibernate.annotations.Nationalized;

import java.time.Instant;
import java.util.UUID;

@Getter
@Setter
@Entity
@Table(name = "Schedule", schema = "dbo")
public class Schedule {
    @Id
    @Column(name = "scheduleId", nullable = false)
    private UUID id;

    @Column(name = "memberId", nullable = false)
    private UUID memberId;

    @Nationalized
    @Column(name = "title", nullable = false)
    private String title;

    @Nationalized
    @Lob
    @Column(name = "description")
    private String description;

    @Column(name = "startTime")
    private Instant startTime;

    @Column(name = "endTime")
    private Instant endTime;

    @ColumnDefault("sysdatetime()")
    @Column(name = "createdAt")
    private Instant createdAt;


}